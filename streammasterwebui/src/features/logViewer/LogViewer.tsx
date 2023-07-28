
import React from "react";
import type * as StreamMasterApi from '../../store/iptvApi';
import DataSelector from "../dataSelector/DataSelector";
import { type ColumnMeta } from "../dataSelector/DataSelectorTypes";
import { formatJSONDateString } from "../../common/common";
import * as Hub from "../../store/signlar_functions";

const LogViewer = () => {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const [maxLines, setMaxLines] = React.useState<number | null>(null);
  const [lastLogId, setLastLogId] = React.useState<number>(0);
  const [dataSource, setDataSource] = React.useState<StreamMasterApi.LogEntryDto[]>([] as StreamMasterApi.LogEntryDto[]);

  const getLogData = React.useCallback(() => {
    // console.debug('LogViewer: ', lastLogId);

    Hub.GetLogRequest({ lastId: lastLogId, maxLines: 5000 } as StreamMasterApi.GetLog)
      .then((returnData) => {
        if (returnData !== null && returnData !== undefined && returnData.length > 0) {
          // console.debug('dataSource: ', dataSource.length);
          // console.debug('returnData: ', returnData.length);

          setDataSource([...dataSource, ...returnData].slice(-1000));
          setLastLogId(returnData[returnData.length - 1].id ?? 0);
          // console.debug('dataSource: ', [...dataSource, ...returnData].slice(-3));
          // console.debug('lastLogId: ', returnData[returnData.length - 1].id ?? 0, ' dataSource: ', [...dataSource, ...returnData].slice(-1000).length);
        }
      }).catch(() => { })


  }, [dataSource, lastLogId]);

  React.useEffect(() => {
    const intervalId = setInterval(() => {
      getLogData();
    }, 1000);

    return () => {
      clearInterval(intervalId);
    };
  }, [getLogData]);

  const timeStampTemplate = React.useCallback((rowData: StreamMasterApi.LogEntry) => {
    return (<div>{formatJSONDateString(rowData.timeStamp ?? '')}</div>);
  }, []);

  const levelTemplate = React.useCallback((rowData: StreamMasterApi.LogEntry) => {
    switch (rowData.logLevel) {
      case 0:
        return (<div className='text-gray-600'>Trace</div>);
      case 1:
        return (<div className='text-blue-600'>Debug</div>);
      case 2:
        return (<div className='text-green-600'>Information</div>);
      case 3:
        return (<div className='text-yellow-600'>Warning</div>);
      case 4:
        return (<div className='text-red-600'>Error</div>);
      case 5:
        return (<div className='text-purple-600'>Critical</div>);
      case 6:
        return (<div className='text-gray-400'>None</div>);
      default:
        return (<div className='text-gray-500'>Unknown</div>);
    }

  }, []);

  const messageTemplate = React.useCallback((rowData: StreamMasterApi.LogEntry) => {
    return (<div>{rowData.message}</div>);
  }, []);

  const sourceColumns = React.useMemo((): ColumnMeta[] => {
    return [

      {
        bodyTemplate: levelTemplate,
        field: 'logLevelName',

        header: 'Level',
        sortable: true,
        style: {
          maxWidth: '6rem',
          width: '6rem',
        } as React.CSSProperties,
      },

      {
        bodyTemplate: timeStampTemplate,
        field: 'timeStamp',

        header: 'Time',
        sortable: true,
        style: {
          maxWidth: '12rem',
          width: '12rem'
        } as React.CSSProperties,
      },

      // {
      //   field: 'logLevel',
      //   fieldType: 'blank'
      // },

      {
        bodyTemplate: messageTemplate,
        field: 'message',

        header: 'Message',
        sortable: true,
        style: {
          maxWidth: '40rem',
        } as React.CSSProperties,
      },

    ]
  }, [levelTemplate, messageTemplate, timeStampTemplate]);

  return (
    <div className='m3uFilesEditor flex flex-column col-12 flex-shrink-0 ' >
      <DataSelector
        columns={sourceColumns}
        dataSource={dataSource}
        emptyMessage="No Clients Streaming"
        enableExport
        enableState={false}
        enableVirtualScroll
        exportFilename={`StreamMaster_Logs_${new Date().toISOString()}`}
        globalSearchEnabled
        id='LogViewer'
        isLoading={dataSource === undefined || dataSource.length === 0}
        showPagination={false}
        style={{
          height: 'calc(100vh - 100px)',
        }}
        virtualScrollHeight='calc(100vh - 140px)'
      />
    </div>
  );
}

LogViewer.displayName = 'LogViewer';
LogViewer.defaultProps = {
  onChange: null,
  value: null,
};


export default React.memo(LogViewer);